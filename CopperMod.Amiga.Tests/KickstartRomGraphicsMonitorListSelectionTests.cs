using Amiga;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed partial class KickstartRomLayersDifferentialTests
{
    [GraphicsRomTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NativeGraphicsMonitorListRecordsOriginalRomNameOrderAndCloseContract(bool ntsc, bool native)
    {
        Assert.True(TryLoadConfiguredRom(out var rom));
        var context = CreateNativeIntuitionOracle(rom, suppressInputDeviceOverlay: true,
            ntsc: ntsc, ecs: false, chipRamBytes: 512 * 1024);
        try
        {
            Assert.NotEqual(0u, ContinueUntilNativeIntuitionPublished(context));
            RestoreOriginalExecForGraphicsOracle(context);
            var graphics = FindLibrary(context.Bus, context.ExecBase, "graphics.library");
            var list = graphics + (uint)GraphicsLayouts.GfxBaseMonitorList;
            var semaphore = context.Bus.ReadLong(graphics + (uint)GraphicsLayouts.GfxBaseMonitorListSemaphore);
            Assert.NotEqual(0u, semaphore);
            CheckEntries();
            var openEntry = graphics;
            var closeEntry = graphics;
            if (native)
            {
                const uint codeAddress = 0x00400000;
                var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out _, defaultMonitorNtsc: ntsc);
                context.Bus.MapWritableMemory(codeAddress, code);
                openEntry = codeAddress + (uint)entries[GraphicsLvo.OpenMonitor];
                closeEntry = codeAddress + (uint)entries[GraphicsLvo.CloseMonitor];
            }
            var defaultMonitor = context.Bus.ReadLong(graphics + (uint)GraphicsLayouts.GfxBaseDefaultMonitor);
            var originalList = ReadBytes(context.Bus, list, 18);
            var originalDefault = ReadBytes(context.Bus, defaultMonitor, GraphicsLayouts.MonitorSpecSize);
            const int allocationSize = 224;
            var first = context.Allocate(allocationSize);
            var second = context.Allocate(allocationSize);
            var requestName = context.Allocate(64);
            var linkedFirst = false;
            var linkedSecond = false;
            Initialize(first, "second.monitor");
            Initialize(second, "second.monitor");
            var freeBefore = FreeBytes();
            var mismatches = new List<string>();
            try
            {
                Add(first, head: false); linkedFirst = true;
                Add(second, head: false); linkedSecond = true;
                Probe("second.monitor", first, "first duplicate wins");
                Probe("SECOND.MONITOR", 0, "case-sensitive");
                Probe("unknown.monitor", 0, "absent");
                Remove(first); linkedFirst = false;
                Add(first, head: false); linkedFirst = true;
                Probe("second.monitor", second, "list order changes winner");
                Remove(first); linkedFirst = false;
                Name(first + GraphicsLayouts.MonitorSpecSize, ntsc ? "ntsc.monitor" : "pal.monitor");
                Add(first, head: true); linkedFirst = true;
                Probe(ntsc ? "ntsc.monitor" : "pal.monitor", first, "duplicate before default");
                Name(second + GraphicsLayouts.MonitorSpecSize, "default.monitor");
                Remove(second); linkedSecond = false;
                Add(second, head: true); linkedSecond = true;
                Probe("default.monitor", defaultMonitor, "default alias precedes list");
                Probe("DEFAULT.MONITOR", defaultMonitor, "folded default alias precedes list");
                Name(second + GraphicsLayouts.MonitorSpecSize, "");
                Probe("", second, "empty registered name");

                // A caller may still hold a valid reference after unlinking.
                Remove(first); linkedFirst = false;
                context.Bus.WriteWord(first + (uint)GraphicsLayouts.MonitorSpecOpenCount, 1);
                var closed = Close(first);
                _output.WriteLine($"unlinked-close:result={closed}:count={Count(first)}");
                Assert.Equal(0u, closed);
                Assert.Equal((ushort)0, Count(first));
                Assert.Equal(freeBefore, FreeBytes());
                CheckEntries();
            }
            finally
            {
                if (linkedFirst) Remove(first);
                if (linkedSecond) Remove(second);
                context.Free(requestName, 64);
                context.Free(second, allocationSize);
                context.Free(first, allocationSize);
            }
            Assert.Equal(originalList, ReadBytes(context.Bus, list, 18));
            Assert.Equal(originalDefault, ReadBytes(context.Bus, defaultMonitor, GraphicsLayouts.MonitorSpecSize));
            Assert.True(mismatches.Count == 0, string.Join("\n", mismatches));

            void Probe(string name, uint expected, string label)
            {
                Name(requestName, name);
                var beforeList = ReadBytes(context.Bus, list, 18);
                foreach (var id in new uint[] { 0, 0xFFFFFFFF, 0xDEADBEEF })
                {
                    var before = new[] { defaultMonitor, first, second }.ToDictionary(node => node, Count);
                    var opened = context.Invoke(openEntry, native ? 0 : (int)GraphicsLvo.OpenMonitor, state =>
                    { state.A[6] = graphics; state.A[1] = requestName; state.D[0] = id; }).D[0];
                    _output.WriteLine($"list-selector:{label}:id={id:X8}:expected={expected:X8}:actual={opened:X8}");
                    if (opened != expected) mismatches.Add($"{label}:id={id:X8}:expected={expected:X8}:actual={opened:X8}");
                    if (opened != 0)
                    {
                        Assert.Contains(opened, before.Keys);
                        Assert.Equal((ushort)(before[opened] + 1), Count(opened));
                        Assert.Equal(0u, Close(opened));
                    }
                    foreach (var node in before) Assert.Equal(node.Value, Count(node.Key));
                    Assert.Equal(beforeList, ReadBytes(context.Bus, list, 18));
                }
            }
            void Initialize(uint node, string name)
            {
                var bytes = GraphicsMonitorSpecImage.Create(node, node + GraphicsLayouts.MonitorSpecSize, !ntsc, 0, graphics);
                for (var i = 0; i < bytes.Length; i++) context.Bus.WriteByte(node + (uint)i, bytes[i], 0);
                Name(node + GraphicsLayouts.MonitorSpecSize, name);
            }
            void Name(uint address, string name)
            {
                for (var i = 0; i < name.Length; i++) context.Bus.WriteByte(address + (uint)i, (byte)name[i], 0);
                context.Bus.WriteByte(address + (uint)name.Length, 0, 0);
            }
            void Add(uint node, bool head) => EditList(() => context.Invoke(context.ExecBase,
                (int)(head ? ExecLvo.AddHead : ExecLvo.AddTail), state => { state.A[0] = list; state.A[1] = node; }));
            void Remove(uint node) => EditList(() => context.Invoke(context.ExecBase, (int)ExecLvo.Remove, state => state.A[1] = node));
            void EditList(Action action)
            {
                context.Invoke(context.ExecBase, (int)ExecLvo.ObtainSemaphore, state => state.A[0] = semaphore);
                try { action(); }
                finally { context.Invoke(context.ExecBase, (int)ExecLvo.ReleaseSemaphore, state => state.A[0] = semaphore); }
            }
            ushort Count(uint node) => context.Bus.ReadWord(node + (uint)GraphicsLayouts.MonitorSpecOpenCount);
            uint Close(uint node) => context.Invoke(closeEntry, native ? 0 : (int)GraphicsLvo.CloseMonitor,
                state => { state.A[6] = graphics; state.A[0] = node; }).D[0];
            uint FreeBytes() => context.Invoke(context.ExecBase, (int)ExecLvo.AvailMem, state => state.D[1] = 0).D[0];
            void CheckEntries()
            {
                var mapped = ReadBytes(context.Bus, GraphicsProbeRomBase, rom.Length);
                foreach (var lvo in new[] { GraphicsLvo.OpenMonitor, GraphicsLvo.CloseMonitor }) Check(graphics, (int)lvo);
                foreach (var lvo in new[] { ExecLvo.AllocMem, ExecLvo.FreeMem, ExecLvo.AvailMem,
                    ExecLvo.AddHead, ExecLvo.AddTail, ExecLvo.Remove, ExecLvo.ObtainSemaphore, ExecLvo.ReleaseSemaphore }) Check(context.ExecBase, (int)lvo);
                void Check(uint library, int lvo)
                {
                    var vector = unchecked(library + (uint)lvo);
                    Assert.True(IsOriginalGraphicsRomEntry(context.Bus.ReadWord(vector), context.Bus.ReadLong(vector + 2), rom, mapped), $"vector {lvo} must be original ROM");
                }
            }
        }
        finally { context.Machine.Dispose(); }
    }
}
