using Amiga;
using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed partial class KickstartRomLayersDifferentialTests
{
    [GraphicsRomTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeGraphicsMonitorIdRegistrationRecordsOriginalRomContract(bool ntsc)
    {
        // Original SDK Function.offs: private SetDisplayInfoData is -750,
        // (handle,buffer,size,tag,id) in (A0,A1,D0,D1,D2). It is an oracle
        // operation here, not a newly claimed replacement public vector.
        // https://amigadev.elowar.com/read/ADCD_2.1/Includes_and_Autodocs_2._guide/node0550.html
        const int setDisplayInfoData = -750;
        Assert.True(TryLoadConfiguredRom(out var rom));
        var context = CreateNativeIntuitionOracle(rom, suppressInputDeviceOverlay: true,
            ntsc: ntsc, ecs: false, chipRamBytes: 512 * 1024);
        try
        {
            Assert.NotEqual(0u, ContinueUntilNativeIntuitionPublished(context));
            RestoreOriginalExecForGraphicsOracle(context);
            var graphics = FindLibrary(context.Bus, context.ExecBase, "graphics.library");
            var defaultMonitor = context.Bus.ReadLong(graphics + GraphicsLayouts.GfxBaseDefaultMonitor);
            var family = ntsc ? 0x11000u : 0x21000u;
            var originalDefault = ReadBytes(context.Bus, defaultMonitor, GraphicsLayouts.MonitorSpecSize);
            var originalList = ReadBytes(context.Bus, graphics + GraphicsLayouts.GfxBaseMonitorList, 18);
            var buffer = context.Allocate(88);
            var candidate = context.Allocate(224);
            var image = GraphicsMonitorSpecImage.Create(candidate, candidate + 160, !ntsc, 0, graphics);
            Write(candidate, image);
            Write(candidate + 160, System.Text.Encoding.ASCII.GetBytes("id-probe.monitor\0"));
            var linked = false;
            CheckEntries();
            Assert.Equal(88u, Transfer((int)GraphicsLvo.GetDisplayInfoData, family));
            var originalRecord = ReadBytes(context.Bus, buffer, 88);
            Assert.Equal(defaultMonitor, context.Bus.ReadLong(buffer + 16));
            var freeBefore = FreeBytes();
            // Independent SDK mode-key literals, not replacement selector code.
            var keys = new uint[] { 0, 4, 0x8000, 0x8004, 0x8020, 0x8024,
                0x800, 0x804, 0x80, 0x84, 0x400, 0x404, 0x8400, 0x8404,
                0x8420, 0x8424, 0x440, 0x444, 0x8440, 0x8444, 0x8460, 0x8464 };
            var ids = keys.SelectMany(key => new[] { key, key | 0x1000u, family | key }).Distinct().ToArray();
            try
            {
                context.Bus.WriteLong(buffer + 16, candidate);
                var changed = Transfer(setDisplayInfoData, family);
                Assert.Equal(68u, changed);
                Assert.Equal(88u, Transfer((int)GraphicsLvo.GetDisplayInfoData, family));
                var selectedRecord = context.Bus.ReadLong(buffer + 16);
                var freeAfterSet = FreeBytes();
                _output.WriteLine($"monitor-id-registration:ntsc={ntsc}:set-result={changed}:original={defaultMonitor:X8}:candidate={candidate:X8}:readback={selectedRecord:X8}:free-before={freeBefore}:free-after-set={freeAfterSet}");
                Assert.Equal(candidate, selectedRecord);
                Assert.Equal(freeBefore, freeAfterSet);

                foreach (var stage in new[] { "unlinked", "linked", "renamed", "unlinked-again" })
                {
                    var list = graphics + GraphicsLayouts.GfxBaseMonitorList;
                    if (stage == "linked")
                    {
                        EditList(() => context.Invoke(context.ExecBase, (int)ExecLvo.AddHead,
                            state => { state.A[0] = list; state.A[1] = candidate; }));
                        linked = true;
                    }
                    if (stage == "renamed")
                    {
                        Write(candidate + 160, System.Text.Encoding.ASCII.GetBytes("renamed.monitor\0"));
                        context.Bus.WriteWord(candidate + GraphicsLayouts.MonitorSpecFlags, 0);
                    }
                    if (stage == "unlinked-again")
                    {
                        EditList(() => context.Invoke(context.ExecBase, (int)ExecLvo.Remove,
                            state => state.A[1] = candidate));
                        linked = false;
                    }
                    CheckReadback(candidate, stage);
                    foreach (var id in ids)
                    {
                        var defaultCount = Count(defaultMonitor);
                        var candidateCount = Count(candidate);
                        var opened = context.Invoke(graphics, (int)GraphicsLvo.OpenMonitor,
                            state => { state.A[1] = 0; state.D[0] = id; }).D[0];
                        _output.WriteLine($"monitor-id-registration:stage={stage}:id={id:X8}:result={opened:X8}");
                        Assert.Equal(id == 0 ? defaultMonitor : candidate, opened);
                        if (opened != 0)
                        {
                            Assert.Equal((ushort)((opened == candidate ? candidateCount : defaultCount) + 1), Count(opened));
                            Assert.Equal(0u, context.Invoke(graphics, (int)GraphicsLvo.CloseMonitor,
                                state => state.A[0] = opened).D[0]);
                        }
                        Assert.Equal(defaultCount, Count(defaultMonitor));
                        Assert.Equal(candidateCount, Count(candidate));
                    }
                }
                // Null registration is distinct from the default pointer.
                // This hypothesis is qualified by the original-ROM run, not
                // inferred from the replacement's fallback behavior.
                Write(buffer, originalRecord);
                context.Bus.WriteLong(buffer + 16, 0);
                Assert.Equal(68u, Transfer(setDisplayInfoData, family));
                Assert.Equal(88u, Transfer((int)GraphicsLvo.GetDisplayInfoData, family));
                Assert.Equal(0u, context.Bus.ReadLong(buffer + 16));
                CheckReadback(0, "null-mapping");
                foreach (var id in ids)
                {
                    var count = Count(defaultMonitor);
                    var opened = context.Invoke(graphics, (int)GraphicsLvo.OpenMonitor,
                        state => { state.A[1] = 0; state.D[0] = id; }).D[0];
                    _output.WriteLine($"monitor-id-registration:stage=null-mapping:id={id:X8}:result={opened:X8}");
                    Assert.Equal(id == 0 ? defaultMonitor : 0, opened);
                    if (opened != 0)
                        Assert.Equal(0u, context.Invoke(graphics, (int)GraphicsLvo.CloseMonitor,
                            state => state.A[0] = opened).D[0]);
                    Assert.Equal(count, Count(defaultMonitor));
                }
                // Readback is a database operation, not an OpenMonitor. Probe
                // opaque values without ever dereferencing/opening those values.
                // The original record is restored in finally before any free.
                foreach (var rawPointer in new[] { 1u, candidate + 1, 0xFFFFFF60u, 0xDEADBEEFu, uint.MaxValue })
                {
                    Write(buffer, originalRecord);
                    context.Bus.WriteLong(buffer + 16, rawPointer);
                    Assert.Equal(68u, Transfer(setDisplayInfoData, family));
                    CheckReadback(rawPointer, "opaque-pointer");
                }
                Assert.Equal(freeAfterSet, FreeBytes());
            }
            finally
            {
                // Restore the database before releasing its temporary pointer.
                Write(buffer, originalRecord);
                _ = Transfer(setDisplayInfoData, family);
                Assert.Equal(88u, Transfer((int)GraphicsLvo.GetDisplayInfoData, family));
                Assert.Equal(originalRecord, ReadBytes(context.Bus, buffer, 88));
                if (linked)
                    EditList(() => context.Invoke(context.ExecBase, (int)ExecLvo.Remove,
                        state => state.A[1] = candidate));
                CheckEntries();
                context.Free(candidate, 224);
                context.Free(buffer, 88);
            }
            Assert.Equal(originalDefault, ReadBytes(context.Bus, defaultMonitor, GraphicsLayouts.MonitorSpecSize));
            Assert.Equal(originalList, ReadBytes(context.Bus, graphics + GraphicsLayouts.GfxBaseMonitorList, 18));

            uint Transfer(int lvo, uint id) => context.Invoke(graphics, lvo, state =>
            {
                state.A[0] = 0; state.A[1] = buffer;
                state.D[0] = 88; state.D[1] = GraphicsDisplayDatabase.DtagMntr; state.D[2] = id;
            }).D[0];
            void CheckReadback(uint expected, string stage)
            {
                var defaultCount = Count(defaultMonitor);
                var candidateCount = Count(candidate);
                Assert.Equal(88u, Transfer((int)GraphicsLvo.GetDisplayInfoData, family));
                var familyRecord = ReadBytes(context.Bus, buffer, 88);
                // Literal full-record baselines captured from the original ROM
                // in456. Only Mspc varies in this experiment; no replacement
                // builder supplies expected values or field interpretations.
                var expectedFamilyRecord = Convert.FromHexString(ntsc
                    ? "80002000000110000000000300000009000000000081002C002C0034005D00150088003F010600E2001500000000000000000000000036FF0000289F0000000000000000000036FF0000289F0016001A0081002C00019000"
                    : "80002000000210000000000300000009000000000081002C002C002C005D001D00880039013800E2001D00000000000000000000000036FF00002BFF0000000000000000000036FF00002BFF001600160081002C00029000");
                BinaryPrimitives.WriteUInt32BigEndian(expectedFamilyRecord.AsSpan(16, 4), expected);
                Assert.Equal(expectedFamilyRecord, familyRecord);
                if (stage == "unlinked")
                    _output.WriteLine($"monitor-mntr-family:ntsc={ntsc}:family={family:X8}:record={Convert.ToHexString(familyRecord)}");
                var differences = new List<string>();
                foreach (var id in ids)
                {
                    var transferred = Transfer((int)GraphicsLvo.GetDisplayInfoData, id);
                    var readback = context.Bus.ReadLong(buffer + 16);
                    _output.WriteLine($"monitor-mntr-registration:stage={stage}:id={id:X8}:bytes={transferred}:mspc={readback:X8}");
                    Assert.Equal(88u, transferred);
                    Assert.Equal(expected, readback); // ID0 query is a database alias, unlike OpenMonitor(0).
                    var actualRecord = ReadBytes(context.Bus, buffer, 88);
                    if (!familyRecord.AsSpan().SequenceEqual(actualRecord))
                        differences.Add($"id={id:X8}:differences=" + string.Join(",", Enumerable.Range(0, 88)
                            .Where(index => familyRecord[index] != actualRecord[index])
                            .Select(index => $"{index:X2}:{familyRecord[index]:X2}->{actualRecord[index]:X2}")));
                    Assert.Equal(defaultCount, Count(defaultMonitor));
                    Assert.Equal(candidateCount, Count(candidate));
                }
                foreach (var difference in differences) _output.WriteLine("monitor-mntr-record:" + difference);
                Assert.True(differences.Count == 0, string.Join("\n", differences));
            }
            ushort Count(uint monitor) => context.Bus.ReadWord(monitor + GraphicsLayouts.MonitorSpecOpenCount);
            uint FreeBytes() => context.Invoke(context.ExecBase, (int)ExecLvo.AvailMem, state => state.D[1] = 0).D[0];
            void Write(uint address, byte[] bytes)
            { for (var index = 0; index < bytes.Length; index++) context.Bus.WriteByte(address + (uint)index, bytes[index], 0); }
            void EditList(Action edit)
            {
                var semaphore = context.Bus.ReadLong(graphics + GraphicsLayouts.GfxBaseMonitorListSemaphore);
                Assert.NotEqual(0u, semaphore);
                context.Invoke(context.ExecBase, (int)ExecLvo.ObtainSemaphore, state => state.A[0] = semaphore);
                try { edit(); }
                finally { context.Invoke(context.ExecBase, (int)ExecLvo.ReleaseSemaphore, state => state.A[0] = semaphore); }
            }
            void CheckEntries()
            {
                var mapped = ReadBytes(context.Bus, GraphicsProbeRomBase, rom.Length);
                foreach (var lvo in new[] { -714, -720, -750, -756 }) Check(graphics, lvo);
                foreach (var lvo in new[] { ExecLvo.AllocMem, ExecLvo.FreeMem, ExecLvo.AvailMem,
                    ExecLvo.AddHead, ExecLvo.Remove, ExecLvo.ObtainSemaphore, ExecLvo.ReleaseSemaphore })
                    Check(context.ExecBase, (int)lvo);
                void Check(uint library, int lvo)
                {
                    var vector = unchecked(library + (uint)lvo);
                    Assert.True(IsOriginalGraphicsRomEntry(context.Bus.ReadWord(vector), context.Bus.ReadLong(vector + 2), rom, mapped), $"Original ROM vector required: {lvo}");
                }
            }
        }
        finally { context.Machine.Dispose(); }
    }
}
