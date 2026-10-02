using Amiga;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed partial class KickstartRomLayersDifferentialTests
{
    [GraphicsRomTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeGraphicsMonitorSelectionRecordsOriginalRomContract(bool ntsc)
    {
        Assert.True(TryLoadConfiguredRom(out var rom));
        var context = CreateNativeIntuitionOracle(rom, suppressInputDeviceOverlay: true,
            ntsc: ntsc, ecs: false, chipRamBytes: 512 * 1024);
        try
        {
            Assert.NotEqual(0u, ContinueUntilNativeIntuitionPublished(context));
            RestoreOriginalExecForGraphicsOracle(context);
            var graphics = FindLibrary(context.Bus, context.ExecBase, "graphics.library");
            AssertOriginalGraphicsRomEntries(context, graphics, rom);
            CheckSelectionEntries();
            var defaultMonitor = context.Bus.ReadLong(graphics + (uint)GraphicsLayouts.GfxBaseDefaultMonitor);
            Assert.NotEqual(0u, defaultMonitor);
            var listBefore = ReadBytes(context.Bus, graphics + 0x180, 18);
            var residents = new Dictionary<uint, ushort>();
            var node = context.Bus.ReadLong(graphics + 0x180);
            while (node != graphics + 0x184)
            {
                Assert.NotEqual(0u, node);
                Assert.True(residents.Count < 16 && !residents.ContainsKey(node), "Invalid or cyclic original monitor list.");
                var count = Count(node);
                residents.Add(node, count);
                _output.WriteLine($"resident:ntsc={ntsc}:default={node == defaultMonitor}:name={Name(context.Bus.ReadLong(node + (uint)GraphicsLayouts.MonitorSpecNodeName))}:count={count}");
                node = context.Bus.ReadLong(node);
            }
            Assert.Contains(defaultMonitor, residents.Keys);
            var nameBuffer = context.Allocate(64);
            var freeBefore = context.Invoke(context.ExecBase, (int)ExecLvo.AvailMem, state => state.D[1] = 0).D[0];
            var requests = new List<(string? Name, uint Id)>();
            foreach (var id in new uint[] { 0xFFFFFFFF, 0, 4, 0x8000, 0x1000, 0x11000, 0x19004, 0x21000, 0x29004, 0x0000FFFF, 0x00031000, 0xDEADBEEF })
                requests.Add((null, id));
            foreach (var name in new[] { "default.monitor", "DEFAULT.MONITOR", "DeFaUlT.MoNiToR", "pal.monitor", "PAL.MONITOR", "ntsc.monitor", "NTSC.MONITOR", "", "unknown.monitor", "pal", "ntsc", ntsc ? "nTsc.monitor" : "pAl.monitor" })
            {
                requests.Add((name, 0xFFFFFFFF));
                requests.Add((name, 0xDEADBEEF));
            }
            // Independent literal selector matrix: include every OCS/ECS key,
            // unsupported feature/control bits, and IDs with/without bit12.
            // Do not use replacement ModeNotAvailable as the ROM oracle.
            var supportedKeys = new uint[] { 0, 4, 0x8000, 0x8004, 0x8020, 0x8024,
                0x800, 0x804, 0x80, 0x84, 0x400, 0x404, 0x8400, 0x8404,
                0x8420, 0x8424, 0x440, 0x444, 0x8440, 0x8444, 0x8460, 0x8464 };
            var probeKeys = supportedKeys.Concat(new uint[] { 1, 2, 8, 0x20, 0x40, 0x100,
                0x200, 0x2000, 0x4000, 0x8008, 0x8800, 0xFFFF }).ToArray();
            foreach (var family in new uint[] { 0, 0x10000, 0x20000, 0x30000 })
            foreach (var marker in new uint[] { 0, 0x1000 })
            foreach (var key in probeKeys)
            {
                var request = ((string?)null, family | marker | key);
                if (!requests.Contains(request)) requests.Add(request);
            }
            var selectorFailures = new List<string>();
            _output.WriteLine($"selector-request-count:{requests.Count}");
            for (var pass = 0; pass < 2; pass++)
            {
              var passFreeBefore = FreeBytes();
              foreach (var request in requests)
              {
                var requestFreeBefore = FreeBytes();
                if (request.Name is not null)
                {
                    for (var i = 0; i < request.Name.Length; i++) context.Bus.WriteByte(nameBuffer + (uint)i, (byte)request.Name[i], 0);
                    context.Bus.WriteByte(nameBuffer + (uint)request.Name.Length, 0, 0);
                }
                var opened = context.Invoke(graphics, (int)GraphicsLvo.OpenMonitor, state =>
                {
                    state.A[1] = request.Name is null ? 0 : nameBuffer;
                    state.D[0] = request.Id;
                }).D[0];
                _output.WriteLine($"selector:pass={pass}:ntsc={ntsc}:name={request.Name ?? "<null>"}:id={request.Id:X8}:result={(opened == 0 ? "null" : opened == defaultMonitor ? "default" : Name(context.Bus.ReadLong(opened + (uint)GraphicsLayouts.MonitorSpecNodeName)))}");
                var expected = request.Name is null
                    ? (request.Id >> 16 == 0 ||
                       (request.Id >> 16 == (ntsc ? 1u : 2u) && (request.Id & 0x1000) != 0)) &&
                      supportedKeys.Contains(request.Id & 0xEFFF)
                    : request.Name is "default.monitor" or "DEFAULT.MONITOR" or "DeFaUlT.MoNiToR" ||
                      request.Name == (ntsc ? "ntsc.monitor" : "pal.monitor");
                if (opened != (expected ? defaultMonitor : 0))
                    selectorFailures.Add($"pass={pass},name={request.Name ?? "<null>"},id={request.Id:X8},expected={expected},opened={opened:X8}");
                if (opened != 0)
                {
                    Assert.True(residents.TryGetValue(opened, out var before), "OpenMonitor unexpectedly created a resident.");
                    Assert.Equal((ushort)(before + 1), Count(opened));
                    var closed = context.Invoke(graphics, (int)GraphicsLvo.CloseMonitor, state => state.A[0] = opened).D[0];
                    Assert.Equal(0u, closed);
                    Assert.Equal(before, Count(opened));
                }
                foreach (var resident in residents) Assert.Equal(resident.Value, Count(resident.Key));
                Assert.Equal(listBefore, ReadBytes(context.Bus, graphics + 0x180, 18));
                var requestFreeAfter = FreeBytes();
                if (requestFreeBefore != requestFreeAfter)
                    _output.WriteLine($"allocation:pass={pass}:name={request.Name ?? "<null>"}:id={request.Id:X8}:before={requestFreeBefore}:after={requestFreeAfter}");
              }
              var passFreeAfter = FreeBytes();
              _output.WriteLine($"allocation-pass:{pass}:before={passFreeBefore}:after={passFreeAfter}");
              if (pass != 0) Assert.Equal(passFreeBefore, passFreeAfter);
            }
            _output.WriteLine($"allocation-total:before={freeBefore}:after={FreeBytes()}");
            // ID registration must not be inferred from mutable public names
            // or timing flags. Probe each independently and restore it before
            // moving on; use original ROM entries throughout.
            var originalName = context.Bus.ReadLong(defaultMonitor + (uint)GraphicsLayouts.MonitorSpecNodeName);
            var originalFlags = context.Bus.ReadWord(defaultMonitor + (uint)GraphicsLayouts.MonitorSpecFlags);
            for (var i = 0; i < "renamed.monitor".Length; i++) context.Bus.WriteByte(nameBuffer + (uint)i, (byte)"renamed.monitor"[i], 0);
            context.Bus.WriteByte(nameBuffer + 15, 0, 0);
            foreach (var mutation in new[] { "name", "flags" })
            {
                if (mutation == "name") context.Bus.WriteLong(defaultMonitor + (uint)GraphicsLayouts.MonitorSpecNodeName, nameBuffer);
                else context.Bus.WriteWord(defaultMonitor + (uint)GraphicsLayouts.MonitorSpecFlags, 0);
                try
                {
                    var opened = context.Invoke(graphics, (int)GraphicsLvo.OpenMonitor,
                        state => state.D[0] = ntsc ? 0x11000u : 0x21000u).D[0];
                    Assert.Equal(defaultMonitor, opened);
                    Assert.Equal(0u, context.Invoke(graphics, (int)GraphicsLvo.CloseMonitor, state => state.A[0] = opened).D[0]);
                    Assert.Equal(residents[defaultMonitor], Count(defaultMonitor));
                }
                finally
                {
                    context.Bus.WriteLong(defaultMonitor + (uint)GraphicsLayouts.MonitorSpecNodeName, originalName);
                    context.Bus.WriteWord(defaultMonitor + (uint)GraphicsLayouts.MonitorSpecFlags, originalFlags);
                }
            }
            context.Free(nameBuffer, 64);
            AssertOriginalGraphicsRomEntries(context, graphics, rom);
            CheckSelectionEntries();
            Assert.True(selectorFailures.Count == 0, string.Join("\n", selectorFailures));

            void CheckSelectionEntries()
            {
                var mapped = ReadBytes(context.Bus, GraphicsProbeRomBase, rom.Length);
                foreach (var lvo in new[] { GraphicsLvo.OpenMonitor, GraphicsLvo.CloseMonitor })
                {
                    var vector = unchecked(graphics + (uint)(int)lvo);
                    Assert.True(IsOriginalGraphicsRomEntry(context.Bus.ReadWord(vector),
                        context.Bus.ReadLong(vector + 2), rom, mapped), $"{lvo} must be original ROM.");
                }
                foreach (var lvo in new[] { ExecLvo.AllocMem, ExecLvo.FreeMem, ExecLvo.AvailMem })
                {
                    var vector = unchecked(context.ExecBase + (uint)(int)lvo);
                    Assert.True(IsOriginalGraphicsRomEntry(context.Bus.ReadWord(vector),
                        context.Bus.ReadLong(vector + 2), rom, mapped), $"{lvo} must be original ROM.");
                }
            }

            ushort Count(uint address) => context.Bus.ReadWord(address + (uint)GraphicsLayouts.MonitorSpecOpenCount);
            uint FreeBytes() => context.Invoke(context.ExecBase, (int)ExecLvo.AvailMem, state => state.D[1] = 0).D[0];
            string Name(uint address)
            {
                Assert.NotEqual(0u, address);
                var value = new System.Text.StringBuilder();
                for (uint i = 0; i < 64; i++)
                {
                    var ch = context.Bus.ReadByte(address + i);
                    if (ch == 0) return value.ToString();
                    value.Append((char)ch);
                }
                throw new Xunit.Sdk.XunitException("Original MonitorSpec name lacks a bounded terminator.");
            }
        }
        finally { context.Machine.Dispose(); }
    }
}
