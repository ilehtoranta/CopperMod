using Copper68k;

namespace Copper68k.Tests.Synthetic;

// Specifications are architectural, not claims of desktop or physical timing readiness.
internal sealed record ModelSpec(string Id, M68kCpuModel Model, int AddressBits, bool FullIndex, bool Diagnostic = false, bool A1200 = false)
{
    public static readonly ModelSpec[] All =
    [
        new("68000", M68kCpuModel.M68000, 24, false),
        new("68010", M68kCpuModel.M68010, 24, false, true),
        new("68EC020", M68kCpuModel.M68EC020, 24, true),
        new("68020", M68kCpuModel.M68020, 32, true),
        new("68030", M68kCpuModel.M68030, 32, true),
        new("68040", M68kCpuModel.M68040, 32, true),
        new("68060", M68kCpuModel.M68060, 32, true, true),
        new("A1200", M68kCpuModel.M68EC020, 24, true, A1200: true)
    ];
    public uint Physical(uint address) => AddressBits == 24 ? address & 0x00ff_ffff : address;
    public string DataAlignment => FullIndex ? "unaligned byte/word/long allowed" : "word/long require even address, else vector 3";
    public string IndexRules => FullIndex ? "brief/full; signed W/L index; scales 1/2/4/8" : "brief; signed W/L index; scale/format bits ignored";
    public string StackRules => Id is "68020" or "68EC020" or "68030" or "68040" or "A1200" ? "USP/ISP/MSP; byte A7 stride 2" : "USP/SSP; byte A7 stride 2";
    public IM68kCore Create(SparseRecordingBus bus) => A1200
        ? M68kCoreFactory.Default.CreateA1200Ec020(bus)
        : M68kCoreFactory.Default.Create(Model, bus);
}

internal readonly record struct BusAccess(uint Address, int Width, bool Write, uint Value, M68kBusAccessKind Kind);

internal sealed class SparseRecordingBus : IM68kBus, IM68kModuleAccessBus
{
    public Dictionary<uint, byte> Memory { get; } = [];
    public List<BusAccess> Accesses { get; } = [];
    public Dictionary<uint, uint> ModuleRegisters { get; } = [];
    public bool ModuleResponder { get; set; }
    public bool TryReadModuleByte(uint address, out byte value) { value = (byte)ModuleRegisters.GetValueOrDefault(address); return ModuleResponder; }
    public bool TryWriteModuleByte(uint address, byte value) { if (ModuleResponder) ModuleRegisters[address] = value; return ModuleResponder; }
    public bool TryWriteModuleLong(uint address, uint value) { if (ModuleResponder) ModuleRegisters[address] = value; return ModuleResponder; }
    public int DeviceResets { get; private set; }
    public void Clear() { Memory.Clear(); Accesses.Clear(); ModuleRegisters.Clear(); ModuleResponder = false; DeviceResets = 0; }
    public byte Peek(uint address) => Memory.GetValueOrDefault(address);
    public uint Peek(uint address, int width)
    {
        uint value = 0;
        for (var i = 0; i < width; i++) value = (value << 8) | Peek(unchecked(address + (uint)i));
        return value;
    }
    public void Initialize(uint address, uint value, int width)
    {
        for (var i = 0; i < width; i++) Memory[unchecked(address + (uint)i)] = (byte)(value >> (8 * (width - 1 - i)));
    }
    private uint Read(uint address, int width, M68kBusAccessKind kind)
    {
        var value = Peek(address, width);
        Accesses.Add(new(address, width, false, value, kind));
        return value;
    }
    private void Write(uint address, uint value, int width, M68kBusAccessKind kind)
    {
        Accesses.Add(new(address, width, true, value, kind));
        Initialize(address, value, width);
    }
    public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind kind) => (byte)Read(address, 1, kind);
    public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind kind) => (ushort)Read(address, 2, kind);
    public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind kind) => Read(address, 4, kind);
    public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind kind) => Write(address, value, 1, kind);
    public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind kind) => Write(address, value, 2, kind);
    public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind kind) => Write(address, value, 4, kind);
    public void ResetExternalDevices(long cycle) { DeviceResets++; }
}

internal sealed class SyntheticMachine(ModelSpec model)
{
    public const uint Code = 0x1000;
    public SparseRecordingBus Bus { get; } = new();
    private IM68kCore? core;
    public IM68kCore Core => core ??= Model.Create(Bus);
    public ModelSpec Model { get; } = model;
    public uint PeekPhysical(uint address, int width)
    {
        uint value = 0;
        for (var i = 0; i < width; i++) value = (value << 8) | Bus.Peek(Model.Physical(unchecked(address + (uint)i)));
        return value;
    }
    public void InitializePhysical(uint address, uint value, int width)
    {
        for (var i = 0; i < width; i++) Bus.Initialize(Model.Physical(unchecked(address + (uint)i)), (byte)(value >> (8 * (width - 1 - i))), 1);
    }
    public void Reset(int ccr = 0, bool supervisor = true)
    {
        Bus.Clear();
        for (uint vector = 0; vector < 256; vector++) Bus.Initialize(vector * 4, 0x9000 + vector * 0x10, 4);
        // Fetch initialization precedes reset. The test never modifies code after reset.
        Core.Reset(Code, 0x8000);
        for (var i = 0; i < 8; i++)
        {
            Core.State.D[i] = 0xa55a0022u + (uint)i * 0x100;
            Core.State.A[i] = 0x4000u + (uint)i * 0x100;
        }
        Core.State.D[7] = 2;
        Core.State.SetUserStackPointer(0x7800);
        Core.State.StatusRegister = (ushort)((supervisor ? 0x2700 : 0x0700) | ccr);
        Core.State.SetActiveStackPointer(supervisor ? 0x4700u : 0x7800u);
    }
    public void Start()
    {
        // Flush prefetched reset data only after all fixture code has been installed.
        var savedD = (uint[])Core.State.D.Clone();
        var savedA = (uint[])Core.State.A.Clone();
        var sr = Core.State.StatusRegister;
        Core.Reset(Code, 0x8000);
        savedD.CopyTo(Core.State.D, 0);
        for (var i = 0; i < 7; i++) Core.State.A[i] = savedA[i];
        Core.State.SetUserStackPointer(0x7800);
        Core.State.StatusRegister = sr;
        Core.State.SetActiveStackPointer(savedA[7]);
        Bus.Accesses.Clear();
    }
}
