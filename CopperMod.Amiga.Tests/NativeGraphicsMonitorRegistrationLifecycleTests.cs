using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsMonitorRegistrationLifecycleTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var ntsc in new[] { false, true })
        foreach (var address in new[] { 0x00400000u, 0x00900000u })
        foreach (var phase in new[] { "before-allocation", "after-allocation", "release" })
        foreach (var fault in new[] { "none", "tag", "version", "owner", "extent", "public-pointer",
            "null-db", "odd-db", "word-db", "wrap-db", "magic", "db-version", "db-size",
            "ntsc-record", "pal-record", "default-id", "occupied" })
            yield return new object[] { ntsc, address, phase, fault };
        foreach (var ntsc in new[] { false, true })
        foreach (var address in new[] { 0x00400000u, 0x00900000u })
        foreach (var fault in new[] { "both-alias", "other-alias", "both-foreign", "new-owner" })
            yield return new object[] { ntsc, address, "release", fault };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void NativeRegistrationPublicationAndReleasePreserveOwnership(
        bool ntsc, uint address, string phase, string fault)
    {
        const uint graphics = 0x00600000, database = 0x00500000, monitor = 0x00510000,
            foreign = 0x00520000, exec = 0x00700000, stack = 0x00800100, returned = 0x00800180;
        var bus = new AmigaBus();
        var positive = GraphicsLibraryImageLayout.CreateGuestImage(graphics,
            GraphicsLibraryImageLayout.NativeMonitorImageSize, 0x420,
            GraphicsLibraryImageLayout.NativeMonitorImageSize, 40, 68,
            "graphics.library", "graphics.library 40.68",
            ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal, true, true);
        var databaseImage = GraphicsDisplayDatabase.CreateNativeDatabaseImage(false, ntsc);
        var monitorImage = GraphicsMonitorSpecImage.CreateNativeDefaultImage(ntsc);
        var selectedSlot = database + (uint)GraphicsDisplayDatabase.NativeMonitorRegistrationsOffset + (ntsc ? 0u : 4u);
        var otherSlot = database + (uint)GraphicsDisplayDatabase.NativeMonitorRegistrationsOffset + (ntsc ? 4u : 0u);
        bus.MapWritableMemory(graphics, positive);
        bus.MapWritableMemory(database, databaseImage);
        bus.MapWritableMemory(monitor, Enumerable.Repeat((byte)0xC7, monitorImage.Length).ToArray());
        bus.MapWritableMemory(stack - 0x100, Enumerable.Repeat((byte)0x5A, 0x200).ToArray());
        bus.WriteLong(graphics + 0x250, GraphicsLibraryImageLayout.NativeRuntimeDescriptorValidTag);
        bus.WriteLong(graphics + 0x258, graphics);
        bus.WriteLong(graphics + 0x25C, database);
        bus.WriteLong(graphics + 0x260, (uint)databaseImage.Length);
        bus.WriteLong(graphics + (uint)GraphicsLayouts.GfxBaseDisplayInfoDataBase, database);
        bus.WriteLong(otherSlot, foreign); // publication must not touch another family's mapping
        var expectedLibrary = Read(graphics, positive.Length);
        var expectedDatabase = Read(database, databaseImage.Length);
        var expectedMonitor = Read(monitor, monitorImage.Length);
        var allocations = 0;
        var frees = 0;
        var preparingRelease = phase == "release";
        bus.RegisterHostGateway(exec - 198, state =>
        {
            allocations++;
            Assert.Equal((uint)monitorImage.Length, state.D[0]);
            Assert.Equal(0x10001u, state.D[1]);
            Assert.Equal(exec, state.A[6]);
            if (phase == "after-allocation") { Mutate(); Snapshot(); }
            state.D[0] = monitor;
            state.D[1] = state.A[0] = state.A[1] = 0xDEADBEEF;
        });
        bus.RegisterHostGateway(exec - 210, state =>
        {
            frees++;
            Assert.Equal(monitor, state.A[1]);
            Assert.Equal((uint)monitorImage.Length, state.D[0]);
            Assert.Equal(exec, state.A[6]);
            Assert.Equal(expectedLibrary, Read(graphics, positive.Length));
            Assert.Equal(expectedDatabase, Read(database, databaseImage.Length));
            Assert.Equal(expectedMonitor, Read(monitor, monitorImage.Length));
            if (phase == "release" && fault == "new-owner")
            {
                bus.WriteLong(selectedSlot, foreign);
                bus.WriteLong(graphics + 0x264, 0xCAFE1234);
                Snapshot(); // yielded replacement must survive return from FreeMem
            }
            state.D[0] = state.D[1] = state.A[0] = state.A[1] = 0xDEADBEEF;
        });
        using var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
        if (phase == "before-allocation") { Mutate(); Snapshot(); }
        if (phase == "release")
        {
            Run(NativeGraphicsMonitorSpecPublisher.BuildRegistered(ntsc), true);
            Assert.Equal(1, allocations);
            Assert.Equal(monitor, bus.ReadLong(selectedSlot));
            preparingRelease = false;
            Mutate(); Snapshot();
        }
        var success = fault == "none" || phase == "release" && fault is
            "occupied" or "both-alias" or "other-alias" or "both-foreign" or "new-owner";
        if (success && phase == "release")
        {
            foreach (var offset in new[] { 0x264, 0x26C, 0x270, 0x274, 0x278, 0x18E }) ExpectedLong(expectedLibrary, offset, 0);
            ExpectedLong(expectedLibrary, 0x180, graphics + 0x184);
            ExpectedLong(expectedLibrary, 0x188, graphics + 0x180);
            foreach (var slot in new[] { selectedSlot, otherSlot })
                if (bus.ReadLong(slot) == monitor) ExpectedLong(expectedDatabase, (int)(slot - database), 0);
        }
        Run(phase == "release" ? NativeGraphicsMonitorSpecRelease.BuildRegistered(ntsc)
            : NativeGraphicsMonitorSpecPublisher.BuildRegistered(ntsc), success);
        Assert.Equal(phase == "before-allocation" && !success ? 0 : 1, allocations);
        Assert.Equal(phase == "release" && success || phase == "after-allocation" && !success ? 1 : 0, frees);
        if (phase == "release" || !success)
        {
            Assert.Equal(expectedLibrary, Read(graphics, positive.Length));
            Assert.Equal(expectedDatabase, Read(database, databaseImage.Length));
            Assert.Equal(expectedMonitor, Read(monitor, monitorImage.Length));
        }
        else
        {
            ExpectedLong(expectedDatabase, (int)(selectedSlot - database), monitor);
            Assert.Equal(expectedDatabase, Read(database, databaseImage.Length));
            Assert.Equal(GraphicsLibraryImageLayout.NativeMonitorDescriptorValidTag, bus.ReadLong(graphics + 0x264));
            Assert.Equal(monitor, bus.ReadLong(graphics + 0x18E));
        }

        void Run(byte[] code, bool expectedSuccess)
        {
            bus.MapWritableMemory(address, code);
            bus.WriteLong(stack, returned);
            cpu.Reset(address, stack);
            cpu.State.D[0] = graphics;
            for (var i = 2; i < 8; i++) cpu.State.D[i] = 0xD0000000u + (uint)i;
            for (var i = 2; i < 6; i++) cpu.State.A[i] = 0xA0000000u + (uint)i;
            cpu.State.A[6] = exec;
            var publishing = phase != "release" || preparingRelease;
            for (var i = 0; i < 4000 && cpu.State.ProgramCounter != returned; i++)
            {
                var pc = cpu.State.ProgramCounter;
                if (publishing && pc >= address && pc < address + code.Length &&
                    bus.ReadWord(pc) == 0x2543 && bus.ReadWord(pc + 2) == 0x180)
                {
                    Assert.Equal(monitor, bus.ReadLong(selectedSlot));
                    Assert.Equal(0u, bus.ReadLong(graphics + 0x264));
                    Assert.Equal(graphics, bus.ReadLong(monitor + (uint)GraphicsLayouts.ExtendedNodeLibrary));
                    Assert.Equal(graphics + 0x184, bus.ReadLong(monitor));
                    Assert.Equal(graphics + 0x180, bus.ReadLong(monitor + 4));
                }
                cpu.ExecuteInstruction();
            }
            Assert.Equal(returned, cpu.State.ProgramCounter);
            Assert.Equal(expectedSuccess ? graphics : 0, cpu.State.D[0]);
            Assert.Equal(stack + 4, cpu.State.A[7]);
            for (var i = 2; i < 8; i++) Assert.Equal(0xD0000000u + (uint)i, cpu.State.D[i]);
            for (var i = 2; i < 6; i++) Assert.Equal(0xA0000000u + (uint)i, cpu.State.A[i]);
            Assert.Equal(exec, cpu.State.A[6]);
            Assert.Equal(0x5A5A5A5Au, bus.ReadLong(stack - 24));
        }
        void Mutate()
        {
            var descriptor = fault switch { "tag" => 0x250, "version" => 0x254, "owner" => 0x258,
                "extent" => 0x260, "public-pointer" => GraphicsLayouts.GfxBaseDisplayInfoDataBase, _ => -1 };
            if (descriptor >= 0) bus.WriteLong(graphics + (uint)descriptor, 0xBAD0BAD0);
            if (fault is "null-db" or "odd-db" or "word-db" or "wrap-db")
            {
                var pointer = fault switch { "null-db" => 0u, "odd-db" => database + 1,
                    "word-db" => database + 2, _ => 0xFFFFFFFCu };
                bus.WriteLong(graphics + 0x25C, pointer);
                bus.WriteLong(graphics + (uint)GraphicsLayouts.GfxBaseDisplayInfoDataBase, pointer);
            }
            var dbOffset = fault switch { "magic" => 0, "db-version" => 4, "db-size" => 8,
                "ntsc-record" => GraphicsDisplayDatabase.NativeMonitorPositionsOffset,
                "pal-record" => GraphicsDisplayDatabase.NativeMonitorPositionsOffset + 12,
                "default-id" => GraphicsDisplayDatabase.NativeDefaultMonitorIdOffset, _ => -1 };
            if (dbOffset >= 0) bus.WriteLong(database + (uint)dbOffset, fault == "db-version" ? 2u : 0xBAD0BAD0);
            if (fault is "occupied" or "both-foreign" or "other-alias") bus.WriteLong(selectedSlot, foreign);
            if (fault is "both-alias" or "other-alias") bus.WriteLong(otherSlot, monitor);
        }
        void Snapshot()
        {
            expectedLibrary = Read(graphics, positive.Length);
            expectedDatabase = Read(database, databaseImage.Length);
            expectedMonitor = Read(monitor, monitorImage.Length);
        }
        byte[] Read(uint pointer, int length) => Enumerable.Range(0, length).Select(i => bus.ReadByte(pointer + (uint)i)).ToArray();
        static void ExpectedLong(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset, 4), value);
    }
}
