using System;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga;

namespace CopperMod.Amiga.CopperStart.Dos;

/// <summary>Concrete reset-scoped bridge for CopperStart dos.library services.</summary>
internal sealed class CopperStartDosContext
{
    public CopperStartDosContext(
        HostGuestMemory memory,
        uint currentDirectoryLock,
        Func<uint, string> readPath,
        Func<string, byte[]?> readFile,
        Func<string, AmigaDosDirectoryEntry?> findEntry,
        Action<uint, AmigaDosDirectoryEntry> writeFileInfoBlock,
        Func<int, uint, uint> allocateMemory,
        Func<uint, int, string> readText,
        Action<string, string> addDiagnostic)
    {
        Memory = memory ?? throw new ArgumentNullException(nameof(memory));
        CurrentDirectoryLock = currentDirectoryLock;
        ReadPath = readPath ?? throw new ArgumentNullException(nameof(readPath));
        ReadFile = readFile ?? throw new ArgumentNullException(nameof(readFile));
        FindEntry = findEntry ?? throw new ArgumentNullException(nameof(findEntry));
        WriteFileInfoBlock = writeFileInfoBlock ?? throw new ArgumentNullException(nameof(writeFileInfoBlock));
        AllocateMemory = allocateMemory ?? throw new ArgumentNullException(nameof(allocateMemory));
        ReadText = readText ?? throw new ArgumentNullException(nameof(readText));
        AddDiagnostic = addDiagnostic ?? throw new ArgumentNullException(nameof(addDiagnostic));
    }

    public HostGuestMemory Memory { get; }
    public uint CurrentDirectoryLock { get; }
    public Func<uint, string> ReadPath { get; }
    public Func<string, byte[]?> ReadFile { get; }
    public Func<string, AmigaDosDirectoryEntry?> FindEntry { get; }
    public Action<uint, AmigaDosDirectoryEntry> WriteFileInfoBlock { get; }
    public Func<int, uint, uint> AllocateMemory { get; }
    public Func<uint, int, string> ReadText { get; }
    public Action<string, string> AddDiagnostic { get; }
}
